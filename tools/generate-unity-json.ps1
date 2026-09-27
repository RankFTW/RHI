$html = (Invoke-WebRequest -Uri "https://github.com/clshortfuse/renodx/wiki/Mods" -UseBasicParsing).Content
$headings = [System.Text.RegularExpressions.Regex]::Matches($html, '<h[1-6][^>]*>.*?</h[1-6]>', 'IgnoreCase,Singleline')
$unityPos = -1; $endPos = -1
foreach ($m in $headings) {
    $text = [System.Web.HttpUtility]::HtmlDecode([System.Text.RegularExpressions.Regex]::Replace($m.Value, '<[^>]+>', '')).Trim()
    if ($text -match 'Unity\s+Engine' -and $unityPos -lt 0) { $unityPos = $m.Index }
    elseif ($unityPos -ge 0 -and $endPos -lt 0) { $endPos = $m.Index }
}
$slice = $html.Substring($unityPos, $endPos - $unityPos)
$rows = [System.Text.RegularExpressions.Regex]::Matches($slice, '(?s)<tr[^>]*>(.*?)</tr>', 'IgnoreCase')

$BT = [char]96

$formats = @(
    'R11G11B10_FLOAT','R10G10B10A2_TYPELESS','R8G8B8A8_TYPELESS','R8G8B8A8_UNORM',
    'R16G16B16A16_TYPELESS','R16G16B16A16_FLOAT','R16G16B16A16_UNORM',
    'R32G32B32A32_TYPELESS','R32G32B32A32_FLOAT','B8G8R8A8_TYPELESS',
    'B8G8R8A8_UNORM','B8G8R8A8_UNORM_SRGB','R10G10B10A2_UNORM'
)

function Strip-Html([string]$h) {
    $s = [System.Text.RegularExpressions.Regex]::Replace($h, '<br\s*/?>', ' ', 'IgnoreCase')
    $s = [System.Text.RegularExpressions.Regex]::Replace($s, '<[^>]+>', '')
    return [System.Web.HttpUtility]::HtmlDecode($s).Trim()
}

function Bt-Token([string]$val) {
    return $BT + $val + $BT
}

function Bt-Pair([string]$k, [string]$v) {
    return (Bt-Token $k) + ' ' + (Bt-Token $v)
}

function Normalize-Size([string]$s) {
    $s = $s.Trim().ToLower()
    if ($s -match 'output.*size')  { return 'Output Size' }
    if ($s -match 'output.*ratio') { return 'Output Ratio' }
    if ($s -match 'any')           { return 'Any Size' }
    return $null
}

function Parse-Notes([string]$raw) {
    $upgrades = [System.Collections.Generic.List[string]]::new()
    $comment = $raw

    foreach ($fmt in $formats) {
        $p = "(?:Increase\s+$fmt\s+Upgrade|Upgrade\s+$fmt)(?:\s+to\s+((?:Output\s+(?:[Ss]ize|[Rr]atio)|Any\s+[Ss]ize)(?:\s+or\s+(?:Output\s+(?:[Ss]ize|[Rr]atio)|Any\s+[Ss]ize))?))?\.?"
        $m = [System.Text.RegularExpressions.Regex]::Match($comment, $p, 'IgnoreCase')
        if ($m.Success) {
            $sizeRaw = $m.Groups[1].Value
            $firstSize = ($sizeRaw -split '\s+or\s+')[0].Trim()
            $sizeToken = Normalize-Size $firstSize
            if ($sizeToken) {
                $upgrades.Add((Bt-Pair "Upgrade_$fmt" $sizeToken))
            } else {
                $upgrades.Add((Bt-Token "Upgrade_$fmt"))
            }
            $before = $comment.Substring(0, $m.Index).TrimEnd(' ', '.', ',')
            $after  = $comment.Substring($m.Index + $m.Length).TrimStart(' ', '.', ',')
            $comment = ($before + ' ' + $after).Trim()
        }
    }

    if ($comment -match 'Enable\s+Swapchain\s+Proxy') {
        $upgrades.Add((Bt-Pair 'Use_Swapchain_Proxy' '1'))
        $comment = [System.Text.RegularExpressions.Regex]::Replace($comment, 'Enable\s+Swapchain\s+Proxy', '', 'IgnoreCase').Trim(' ', '.', ',')
    }
    if ($comment -match 'Swapchain\s+Encoding:\s*Gamma|(?:Encoding|encoding):\s*Gamma|encoding\s+set\s+to\s+gamma') {
        $upgrades.Add((Bt-Pair 'Swapchain_Encoding' '0'))
        $comment = [System.Text.RegularExpressions.Regex]::Replace($comment, '(?:Swapchain\s+)?[Ee]ncoding[:\s]+[Gg]amma|encoding\s+set\s+to\s+gamma', '', 'IgnoreCase').Trim(' ', '.', ',')
    } elseif ($comment -match 'Swapchain\s+Encoding:\s*scRGB') {
        $upgrades.Add((Bt-Pair 'Swapchain_Encoding' 'scRGB'))
        $comment = [System.Text.RegularExpressions.Regex]::Replace($comment, 'Swapchain\s+Encoding:\s*scRGB', '', 'IgnoreCase').Trim(' ', '.', ',')
    }
    if ($comment -match '(?:Enable\s+)?Force\s+Pipeline\s+Cloning') {
        $upgrades.Add((Bt-Pair 'Force_Pipeline_Cloning' '1'))
        $comment = [System.Text.RegularExpressions.Regex]::Replace($comment, '(?:Enable\s+)?Force\s+Pipeline\s+Cloning', '', 'IgnoreCase').Trim(' ', '.', ',')
    }
    if ($comment -match 'fullscreen\s+windowed\s+borderless|Use\s+borderless') {
        $upgrades.Add((Bt-Pair 'ForceBorderless' '1'))
        $comment = [System.Text.RegularExpressions.Regex]::Replace($comment, 'Use\s+(?:fullscreen\s+windowed\s+)?borderless[^.]*\.?', '', 'IgnoreCase').Trim(' ', '.', ',')
    }

    $comment = [System.Text.RegularExpressions.Regex]::Replace($comment, '\s{2,}', ' ').Trim(' ', '.', ',', ' ')
    if ([string]::IsNullOrWhiteSpace($comment)) { $comment = $null }

    $upStr = $null
    if ($upgrades.Count -gt 0) { $upStr = $upgrades -join ' ' }

    return @{ Upgrades = $upStr; Comments = $comment }
}

$entries = [System.Collections.Generic.List[object]]::new()
foreach ($r in $rows) {
    $cells = [System.Text.RegularExpressions.Regex]::Matches($r.Groups[1].Value, '(?s)<td[^>]*>(.*?)</td>', 'IgnoreCase')
    if ($cells.Count -lt 2) { continue }
    $name = Strip-Html $cells[0].Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($name)) { continue }
    $statusRaw = Strip-Html $cells[1].Groups[1].Value
    $status = if ($statusRaw -match '🚧') { 'WIP' } else { 'Done' }
    $notes = if ($cells.Count -ge 3) { Strip-Html $cells[2].Groups[1].Value } else { '' }
    $parsed = Parse-Notes $notes
    if ($parsed.Upgrades) {
        # Any bare `Upgrade_R11G11B10_FLOAT` with no size → default to Output Size
        $upStr = [System.Text.RegularExpressions.Regex]::Replace(
            $parsed.Upgrades,
            ($BT + 'Upgrade_R11G11B10_FLOAT' + $BT + '(?!\s+' + $BT + ')'),
            ($BT + 'Upgrade_R11G11B10_FLOAT' + $BT + ' ' + $BT + 'Output Size' + $BT))
    } else { $upStr = $null }
    $entries.Add([ordered]@{
        Name     = $name
        Status   = $status
        Upgrades = $upStr
        Comments = $parsed.Comments
    })
}

Write-Host "Total entries: $($entries.Count)"
$b = $entries | Where-Object { $_.Name -eq 'Beholder' }
Write-Host "Beholder: U=[$($b.Upgrades)] C=[$($b.Comments)]"

$opts = [System.Text.Json.JsonSerializerOptions]::new()
$opts.WriteIndented = $true
$opts.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
$json = [System.Text.Json.JsonSerializer]::Serialize([object[]]$entries.ToArray(), $opts)
[System.IO.File]::WriteAllText("G:\RDXC\tools\RenoDXdb-Editor\publish\RenoDXdb-unity.json", $json, [System.Text.UTF8Encoding]::new($false))
Write-Host "Written."
