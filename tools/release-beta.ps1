<#
.SYNOPSIS
  Builds and publishes an RHI beta to RankFTW/rhi-beta in one go.

.DESCRIPTION
  1. Reads the version from RenoDXCommander.csproj: AssemblyVersion (e.g. 2.9.5.0) + RhiBeta (e.g. 1).
  2. Closes RHI and runs publish.bat (skip with -SkipPublish), then checks the built exe really is that beta.
  3. Builds the installer with Inno Setup (RHI Setup.iss) into a temp folder.
  4. Creates the GitHub release in RankFTW/rhi-beta: tag RHI-2.9.5-beta.1, title "RHI 2.9.5 Beta 1",
     asset RHI-Setup.exe, notes from docs/release-notes/v2.9.5-beta-1.md (else the top section of
     RHI_PatchNotes.md). A normal release, not a pre-release: RHI reads /releases/latest, which skips those.

  Nothing is committed or pushed in RankFTW/RHI. See docs/beta-channel.md.

.PARAMETER SkipPublish
  Use the exe already in the publish folder instead of running publish.bat.

.PARAMETER DryRun
  Do everything except creating the GitHub release (prints what it would create).

.EXAMPLE
  pwsh tools/release-beta.ps1
  pwsh tools/release-beta.ps1 -DryRun
#>
param(
    [switch]$SkipPublish,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$Repo       = Split-Path -Parent $PSScriptRoot           # g:\RDXC
$Csproj     = Join-Path $Repo 'RenoDXCommander\RenoDXCommander.csproj'
$PublishDir = 'C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI'
$Iss        = Join-Path $Repo 'RHI Setup.iss'
$Iscc       = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
$BetaRepo   = 'RankFTW/rhi-beta'

function Fail($msg) { Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }

# ── 1. Version ────────────────────────────────────────────────────────────────
[xml]$proj = Get-Content $Csproj -Raw
$asmVersion = ($proj.Project.PropertyGroup | ForEach-Object { $_.AssemblyVersion } | Where-Object { $_ } | Select-Object -First 1)
$beta       = ($proj.Project.PropertyGroup | ForEach-Object { $_.RhiBeta } | Where-Object { $_ } | Select-Object -First 1)
if (-not $asmVersion) { Fail "AssemblyVersion not found in $Csproj" }
if (-not $beta)       { Fail "RhiBeta is empty in the csproj. Set it (1, 2, ...) for a beta build; releases go to RankFTW/RHI, not this script." }
if ($beta -notmatch '^\d+$' -or [int]$beta -lt 1) { Fail "RhiBeta must be a whole number from 1 ('$beta')" }

$v = [Version]$asmVersion
$number = if ($v.Revision -gt 0) { "$($v.Major).$($v.Minor).$($v.Build).$($v.Revision)" } else { "$($v.Major).$($v.Minor).$($v.Build)" }
$tag    = "RHI-$number-beta.$beta"
$title  = "RHI $number Beta $beta"
$label  = "$asmVersion-beta.$beta"                    # the exe's ProductVersion
Write-Host "Beta: $title  (tag $tag, exe label $label)" -ForegroundColor Cyan

# Tag must be new
gh release view $tag --repo $BetaRepo *> $null
if ($LASTEXITCODE -eq 0) { Fail "$BetaRepo already has a release $tag. Bump RhiBeta in the csproj." }

# ── 2. Build ──────────────────────────────────────────────────────────────────
if (-not $SkipPublish) {
    if (Get-Process RHI -ErrorAction SilentlyContinue) {
        Write-Host 'Closing RHI...'
        New-Item -ItemType File -Force (Join-Path $env:LOCALAPPDATA 'RHI\rhi_shutdown_requested') | Out-Null
        $i = 0
        while ((Get-Process RHI -ErrorAction SilentlyContinue) -and $i -lt 30) { Start-Sleep 1; $i++ }
    }
    Write-Host 'Running publish.bat...'
    # publish.bat uses paths relative to the repo, so run it from there
    # Full path: with NoDefaultCurrentDirectoryInExePath set, cmd doesn't look in the current folder
    $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "`"$(Join-Path $Repo 'publish.bat')`"" -WorkingDirectory $Repo -NoNewWindow -Wait -PassThru
    if ($proc.ExitCode -ne 0) { Fail "publish.bat failed (exit $($proc.ExitCode))" }
}

$exe = Join-Path $PublishDir 'RHI.exe'
if (-not (Test-Path $exe)) { Fail "$exe not found" }
$product = (Get-Item $exe).VersionInfo.ProductVersion
if ($product -ne $label) { Fail "The built exe says '$product', expected '$label'. Rebuild (or drop -SkipPublish)." }
Write-Host "Exe OK: $product, built $((Get-Item $exe).LastWriteTime)"

# ── 3. Installer ──────────────────────────────────────────────────────────────
if (-not (Test-Path $Iscc)) { Fail "Inno Setup compiler not found at $Iscc" }
$outDir = Join-Path $env:TEMP "rhi-beta-release\$tag"
New-Item -ItemType Directory -Force $outDir | Out-Null
Write-Host 'Building the installer...'
& $Iscc "/DPublishDir=$PublishDir" "/DInstallerOutputDir=$outDir" /Q $Iss
if ($LASTEXITCODE -ne 0) { Fail "Inno Setup failed (exit $LASTEXITCODE)" }
$setup = Join-Path $outDir 'RHI-Setup.exe'
if (-not (Test-Path $setup)) { Fail "Installer not produced at $setup" }
Write-Host "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"

# ── 4. Notes ──────────────────────────────────────────────────────────────────
$notesFile = Join-Path $Repo "docs\release-notes\v$number-beta-$beta.md"
if (-not (Test-Path $notesFile)) {
    # Fall back to the top version section of the patch notes
    $patch = Get-Content (Join-Path $Repo 'RenoDXCommander\RHI_PatchNotes.md') -Raw
    $m = [regex]::Match($patch, '(?ms)^## v.*?(?=^## v|\z)')
    if (-not $m.Success) { Fail "No notes: create $notesFile" }
    $notesFile = Join-Path $outDir 'notes.md'
    Set-Content -Path $notesFile -Value $m.Value.Trim() -Encoding utf8
    Write-Host "Notes: top section of RHI_PatchNotes.md (no $("v$number-beta-$beta.md") found)" -ForegroundColor Yellow
} else {
    Write-Host "Notes: $notesFile"
}

# ── 5. Release ────────────────────────────────────────────────────────────────
if ($DryRun) {
    Write-Host "`nDry run: would create $BetaRepo release '$title' ($tag) with $setup" -ForegroundColor Yellow
    Write-Host '--- notes ---'; Get-Content $notesFile | Select-Object -First 30
    exit 0
}

gh release create $tag $setup --repo $BetaRepo --title $title --notes-file $notesFile --latest
if ($LASTEXITCODE -ne 0) { Fail 'gh release create failed' }
Write-Host "`nPublished: https://github.com/$BetaRepo/releases/tag/$tag" -ForegroundColor Green
Write-Host "RHI with 'Get beta updates' on will offer it on its next update check."
