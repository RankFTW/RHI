# fetch-unity-mods.ps1
# Fetches the Unity Engine game list from the RenoDX wiki.
# These are the games in the Name / Status / Notes table under the
# "Unity Engine" heading on the Mods page.
# Output: docs\RenoDXdb-unity.json

. "$PSScriptRoot\_wiki-parse-common.ps1"
$OutFile = Join-Path $PSScriptRoot "RenoDXdb-unity.json"

$html = Fetch-WikiHtml

$unityPos = Find-HeadingPos $html 'unity engine'
$depPos   = Find-HeadingPos $html 'deprecated'

if ($unityPos -lt 0) {
    Write-Host "ERROR: Could not find 'Unity Engine' heading on the wiki page."
    exit 1
}

$candidates = @($depPos) | Where-Object { $_ -gt $unityPos }
$endPos = if ($candidates.Count -gt 0) { ($candidates | Measure-Object -Minimum).Minimum } else { -1 }

Write-Host "Unity Engine mods: HTML slice $unityPos → $endPos"
$mods = Parse-ModTables $html $unityPos $endPos 'engine'

Write-JsonArray $mods $OutFile
