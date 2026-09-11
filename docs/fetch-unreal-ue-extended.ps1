# fetch-unreal-ue-extended.ps1
# Fetches the UE Extended game list from the RenoDX wiki.
# These are the games in the Name / Status / Notes table under the
# "UE Extended" heading on the Mods page.
# Output: docs\RenoDXdb-unreal-ue-extended.json

. "$PSScriptRoot\_wiki-parse-common.ps1"
$OutFile = Join-Path $PSScriptRoot "RenoDXdb-unreal-ue-extended.json"

$html = Fetch-WikiHtml

$ueExtPos    = Find-HeadingPos $html 'UE Extended'
$ueLegacyPos = Find-HeadingPos $html '^Unreal Engine\b'

if ($ueExtPos -lt 0) {
    Write-Host "ERROR: Could not find 'UE Extended' heading on the wiki page."
    exit 1
}

# End at the Legacy Unreal Engine heading (or Unity/Deprecated if Legacy not found)
$unityPos = Find-HeadingPos $html 'Unity Engine'
$depPos   = Find-HeadingPos $html 'Deprecated'

$candidates = @($ueLegacyPos, $unityPos, $depPos) | Where-Object { $_ -gt $ueExtPos }
$endPos = if ($candidates.Count -gt 0) { ($candidates | Measure-Object -Minimum).Minimum } else { -1 }

Write-Host "UE Extended mods: HTML slice $ueExtPos → $endPos"
$mods = Parse-ModTables $html $ueExtPos $endPos 'engine'

Write-JsonArray $mods $OutFile
