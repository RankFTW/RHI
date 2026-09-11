# fetch-named-mods.ps1
# Fetches named (per-game) RenoDX mods from the wiki.
# These are the mods in the main table at the top of the Mods page
# (Name / Maintainer / Links / Status columns).
# Output: docs\RenoDXdb.json

. "$PSScriptRoot\_wiki-parse-common.ps1"
$OutFile = Join-Path $PSScriptRoot "RenoDXdb.json"

$html = Fetch-WikiHtml

# Named mods come before any engine-specific heading.
# Stop at the first engine/deprecated heading.
$uePos   = Find-HeadingPos $html 'UE Extended'
$unityPos = Find-HeadingPos $html 'unity engine'
$depPos  = Find-HeadingPos $html 'deprecated'

# End at whichever engine section starts first (excluding -1 = not found)
$candidates = @($uePos, $unityPos, $depPos) | Where-Object { $_ -ge 0 }
$endPos = if ($candidates.Count -gt 0) { ($candidates | Measure-Object -Minimum).Minimum } else { -1 }

Write-Host "Named mods: HTML slice 0 → $endPos"
$mods = Parse-ModTables $html 0 $endPos 'named'

Write-JsonArray $mods $OutFile
