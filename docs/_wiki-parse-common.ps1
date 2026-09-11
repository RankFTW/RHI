# _wiki-parse-common.ps1
# Shared parsing helpers for the RenoDX wiki snapshot scripts.
# Dot-source this file — do not run directly.

$WikiUrl = "https://github.com/clshortfuse/renodx/wiki/Mods"

function Fetch-WikiHtml {
    Write-Host "Fetching $WikiUrl ..."
    return (Invoke-WebRequest -Uri $WikiUrl -UseBasicParsing).Content
}

function Strip-Tags([string]$h) {
    $s = [System.Text.RegularExpressions.Regex]::Replace($h, '<br\s*/?>', "`n", 'IgnoreCase')
    $s = [System.Text.RegularExpressions.Regex]::Replace($s, '<li[^>]*>', "`n• ", 'IgnoreCase')
    $s = [System.Text.RegularExpressions.Regex]::Replace($s, '<[^>]+>', '')
    $s = [System.Web.HttpUtility]::HtmlDecode($s)
    $s = $s -replace '\u2018|\u2019', "'" -replace '\u201C|\u201D', '"'
    return $s.Trim()
}

# Returns the character position of the first <h1>–<h3> heading whose text
# matches $pattern (case-insensitive), or -1 if not found.
function Find-HeadingPos([string]$html, [string]$pattern) {
    # Match heading tags and check their stripped text content
    $headings = [System.Text.RegularExpressions.Regex]::Matches(
        $html, '<h[1-6][^>]*>.*?</h[1-6]>', 'IgnoreCase,Singleline')
    foreach ($m in $headings) {
        $text = Strip-Tags $m.Value
        if ($text -match $pattern) { return $m.Index }
    }
    return -1
}

# Parses all mod tables in $html between positions $startPos and $endPos.
# $tableKind: "named" expects 4-col Name/Maintainer/Links/Status layout.
#             "engine" expects 2-3-col Name/Status/Notes layout.
# Returns a List[hashtable] of mod entries in RenoDXdb format.
function Parse-ModTables([string]$html, [int]$startPos, [int]$endPos, [string]$tableKind) {
    $mods = [System.Collections.Generic.List[hashtable]]::new()

    $tablePattern = '(?s)<table[^>]*>(.*?)</table>'
    $tableMatches = [System.Text.RegularExpressions.Regex]::Matches($html, $tablePattern, 'IgnoreCase')

    foreach ($tableMatch in $tableMatches) {
        $tStart = $tableMatch.Index
        $tEnd   = $tStart + $tableMatch.Length

        # Only process tables within the requested slice
        if ($startPos -ge 0 -and $tStart -lt $startPos) { continue }
        if ($endPos   -ge 0 -and $tStart -ge $endPos)   { continue }

        $tableHtml = $tableMatch.Groups[1].Value

        # Parse header row
        $headerRowMatch = [System.Text.RegularExpressions.Regex]::Match(
            $tableHtml, '(?s)<tr[^>]*>(.*?)</tr>', 'IgnoreCase')
        if (-not $headerRowMatch.Success) { continue }
        $headerHtml  = $headerRowMatch.Groups[1].Value
        $headerCells = [System.Text.RegularExpressions.Regex]::Matches(
            $headerHtml, '(?s)<t[hd][^>]*>(.*?)</t[hd]>', 'IgnoreCase')
        if ($headerCells.Count -lt 2) { continue }

        $headers = @($headerCells | ForEach-Object { (Strip-Tags $_.Groups[1].Value).ToLowerInvariant() })

        $hasLinks  = $headers | Where-Object { $_ -match 'link|download' }
        $hasStatus = $headers | Where-Object { $_ -match 'status' }

        # Determine column roles
        $nameCol = 0; $authorCol = -1; $linksCol = -1; $statusCol = -1; $notesCol = -1
        for ($i = 0; $i -lt $headers.Count; $i++) {
            $h = $headers[$i]
            if    ($h -match 'maintainer|author|developer') { $authorCol  = $i }
            elseif ($h -match 'link|download')              { $linksCol   = $i }
            elseif ($h -match 'status')                     { $statusCol  = $i }
            elseif ($h -match 'note')                       { $notesCol   = $i }
        }

        # For "named" kind, require a Links column (4-col layout).
        # For "engine" kind, require a Status column without a Links column.
        $isNamedTable  = $null -ne $hasLinks
        $isEngineTable = ($null -ne $hasStatus) -and ($null -eq $hasLinks) -and ($headerCells.Count -ge 2)

        if ($tableKind -eq 'named'  -and -not $isNamedTable)  { continue }
        if ($tableKind -eq 'engine' -and -not $isEngineTable) { continue }

        Write-Host "  Table [$tableKind]: $($headerCells.Count) cols [$($headers -join '|')]"

        $rowMatches = [System.Text.RegularExpressions.Regex]::Matches(
            $tableHtml, '(?s)<tr[^>]*>(.*?)</tr>', 'IgnoreCase')
        $isFirst = $true
        foreach ($rowMatch in $rowMatches) {
            if ($isFirst) { $isFirst = $false; continue }

            $rowHtml = $rowMatch.Groups[1].Value
            $cells   = [System.Text.RegularExpressions.Regex]::Matches(
                $rowHtml, '(?s)<td[^>]*>(.*?)</td>', 'IgnoreCase')
            if ($cells.Count -lt 2) { continue }
            $cellArr = @($cells | ForEach-Object { $_.Groups[1].Value })

            # Name
            $name = Strip-Tags $cellArr[$nameCol]
            if ([string]::IsNullOrWhiteSpace($name)) { continue }

            # Author
            $author = ""
            if ($authorCol -ge 0 -and $authorCol -lt $cellArr.Count) {
                $author = Strip-Tags $cellArr[$authorCol]
            }

            # URLs
            $snapshotUrl = $null; $snapshotUrl32 = $null
            $nexusUrl    = $null; $discordUrl    = $null; $discussionUrl = $null
            $scanCells   = if ($linksCol -ge 0) { @($cellArr[$linksCol]) } else { $cellArr }
            foreach ($c in $scanCells) {
                $hrefMatches = [System.Text.RegularExpressions.Regex]::Matches($c, 'href="([^"]+)"')
                foreach ($hm in $hrefMatches) {
                    $href = $hm.Groups[1].Value
                    if    ($href -match '\.addon32$')                               { $snapshotUrl32 = $href }
                    elseif ($href -match '\.addon64$|\.github\.io/renodx/|/releases/download/') {
                        if (-not $snapshotUrl) { $snapshotUrl = $href }
                    }
                    elseif ($href -match 'nexusmods\.com')                           { $nexusUrl      = $href }
                    elseif ($href -match 'discord\.com|discord\.gg')                 { $discordUrl    = $href }
                    elseif ($href -match 'renodx/discussions/')                      { $discussionUrl = $href }
                    elseif ($href -match 'snapshot|download' -and -not $snapshotUrl) { $snapshotUrl   = $href }
                }
            }

            # Status
            $status = "Done"
            if ($statusCol -ge 0 -and $statusCol -lt $cellArr.Count) {
                if ((Strip-Tags $cellArr[$statusCol]) -match '🚧') { $status = "WIP" }
            }

            # Notes — tooltip first, then plain status cell text, then dedicated notes col, then extra cells
            $notes = $null
            if ($statusCol -ge 0 -and $statusCol -lt $cellArr.Count) {
                $tooltipMatch = [System.Text.RegularExpressions.Regex]::Match(
                    $cellArr[$statusCol], 'title="([^"]+)"')
                if ($tooltipMatch.Success) { $notes = $tooltipMatch.Groups[1].Value.Trim() }

                if (-not $notes) {
                    $afterEmoji = (Strip-Tags $cellArr[$statusCol]) -replace '✅|🚧', '' |
                        ForEach-Object { $_.Trim() }
                    if ($afterEmoji) { $notes = $afterEmoji }
                }
            }
            if (-not $notes -and $notesCol -ge 0 -and $notesCol -lt $cellArr.Count) {
                $n = Strip-Tags $cellArr[$notesCol]
                if (-not [string]::IsNullOrWhiteSpace($n)) { $notes = $n.Trim() }
            }
            if (-not $notes) {
                for ($i = 0; $i -lt $cellArr.Count; $i++) {
                    if ($i -eq $nameCol -or $i -eq $authorCol -or
                        $i -eq $linksCol -or $i -eq $statusCol -or $i -eq $notesCol) { continue }
                    $n = Strip-Tags $cellArr[$i]
                    if (-not [string]::IsNullOrWhiteSpace($n)) { $notes = $n.Trim(); break }
                }
            }
            if ([string]::IsNullOrWhiteSpace($notes)) { $notes = $null }

            $mods.Add([ordered]@{
                name          = $name
                status        = $status
                author        = $author
                snapshotUrl   = $snapshotUrl
                snapshotUrl32 = $snapshotUrl32
                nexusUrl      = $nexusUrl
                discordUrl    = $discordUrl
                discussionUrl = $discussionUrl
                notes         = $notes
            })
        }
    }

    return $mods
}

function Write-JsonArray([System.Collections.Generic.List[hashtable]]$mods, [string]$outFile) {
    $json = $mods.ToArray() | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText($outFile, $json, [System.Text.Encoding]::UTF8)
    Write-Host "Done. $($mods.Count) entries written to: $outFile"
}
