# fetch-wiki-snapshot.ps1
# Convenience wrapper — runs all wiki snapshot scripts in one go.
# Use the individual scripts if you only need one category.

& "$PSScriptRoot\fetch-named-mods.ps1"
Write-Host ""
& "$PSScriptRoot\fetch-unreal-ue-extended.ps1"
Write-Host ""
& "$PSScriptRoot\fetch-unreal-legacy.ps1"
Write-Host ""
& "$PSScriptRoot\fetch-unity-mods.ps1"
