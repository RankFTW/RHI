# fetch-unreal-legacy.ps1
# Fetches the Legacy Unreal Engine game list from the RenoDX wiki.
# These are the games in the Name / Status / Notes table under the
# "Unreal Engine" (legacy) heading on the Mods page.
# Output: docs\RenoDXdb-unreal-legacy.json

. "$PSScriptRoot\_wiki-parse-common.ps1"
$OutFile = Join-Path $PSScriptRoot "RenoDXdb-unreal-legacy.json"

$html = Fetch-WikiHtml

$ueLegacyPos = Find-HeadingPos $html '^Unreal Engine\b'
$unityPos    = Find-HeadingPos $html 'Unity Engine'
$depPos      = Find-HeadingPos $html 'Deprecated'

if ($ueLegacyPos -lt 0) {
    Write-Host "ERROR: Could not find 'Unreal Engine' heading on the wiki page."
    exit 1
}

$candidates = @($unityPos, $depPos) | Where-Object { $_ -gt $ueLegacyPos }
$endPos = if ($candidates.Count -gt 0) { ($candidates | Measure-Object -Minimum).Minimum } else { -1 }

Write-Host "Legacy Unreal mods: HTML slice $ueLegacyPos → $endPos"
$mods = Parse-ModTables $html $ueLegacyPos $endPos 'engine'

Write-JsonArray $mods $OutFile
