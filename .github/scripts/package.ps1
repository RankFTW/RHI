# Publishes RHI the same way publish.bat does and builds RHI-Setup.exe with Inno Setup.
# Used by the Build and Release workflows.
#
#   pwsh .github/scripts/package.ps1 -PublishDir <folder> -InstallerDir <folder>
#
# Leaves the ready-to-run app in -PublishDir and RHI-Setup.exe in -InstallerDir.

param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $InstallerDir
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
Set-Location $root
$src = 'RenoDXCommander'

# RenoDXCommander.csproj copies ClrMD from the NuGet cache as a loose file
# (it is deliberately not a PackageReference), so make sure it is there.
$clrmd = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.diagnostics.runtime/3.1.512801/lib/net6.0/Microsoft.Diagnostics.Runtime.dll'
if (-not (Test-Path $clrmd)) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) 'rhi-clrmd'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    Set-Content (Join-Path $tmp 'clrmd.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.Diagnostics.Runtime" Version="3.1.512801" /></ItemGroup></Project>'
    dotnet restore (Join-Path $tmp 'clrmd.csproj') --packages (Join-Path $env:USERPROFILE '.nuget/packages')
    if ($LASTEXITCODE -ne 0) { throw 'ClrMD restore failed' }
}

dotnet publish "$src/RenoDXCommander.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:Platform=x64 --self-contained false -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

dotnet build RHI.DropHelper/RHI.DropHelper.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'drop helper build failed' }
$helper = 'RHI.DropHelper/bin/x64/Release/net8.0-windows'
foreach ($f in 'RHI.DropHelper.exe', 'RHI.DropHelper.dll', 'RHI.DropHelper.runtimeconfig.json') {
    Copy-Item (Join-Path $helper $f) $PublishDir -Force
}

$content = @(
    'icon.ico', '7z.exe', '7z.dll', 'ReShade.ini', 'ReShade.Vulkan.ini', 'ReShade64.json',
    'RHI_PatchNotes.md', 'relimiter.ini', 'reshade.rdr2.ini',
    'OptiScaler_nightly.nvidia.ini', 'OptiScaler_nightly.amd-dlss.ini', 'OptiScaler_nightly.amd-nodlss.ini',
    'OptiScaler_dlssnr.nvidia.ini', 'OptiScaler_dlssnr.amd-dlss.ini', 'OptiScaler_dlssnr.amd-nodlss.ini',
    'OptiScaler_f5.nvidia.ini', 'OptiScaler_f5.amd-dlss.ini', 'OptiScaler_f5.amd-nodlss.ini',
    'OptiScaler.nvidia.ini', 'OptiScaler.amd-dlss.ini', 'OptiScaler.amd-nodlss.ini'
)
foreach ($f in $content) { Copy-Item (Join-Path $src $f) $PublishDir -Force }

$icons = Join-Path $PublishDir 'Assets/icons'
New-Item -ItemType Directory -Force $icons | Out-Null
Copy-Item "$src/Assets/icons/*.ico", "$src/Assets/icons/*.png" $icons -Force

Copy-Item $clrmd $PublishDir -Force

# Installer
function Find-Iscc {
    Get-ChildItem 'C:\Program Files*\Inno Setup *\ISCC.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
$iscc = Find-Iscc
if (-not $iscc) {
    choco install innosetup -y --no-progress
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup install failed' }
    $iscc = Find-Iscc
    if (-not $iscc) { throw 'ISCC.exe not found after installing Inno Setup' }
}
New-Item -ItemType Directory -Force $InstallerDir | Out-Null
& $iscc "/DPublishDir=$((Resolve-Path $PublishDir).Path)" "/DInstallerOutputDir=$((Resolve-Path $InstallerDir).Path)" 'RHI Setup.iss'
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
