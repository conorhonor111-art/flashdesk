<#
    Check-MarkGeometry.ps1 -- the mark's geometry lives in six files by hand, and that already bit
    us once (CLAUDE.md, "THE MARK WAS REFITTED 2026-08-06, AND ITS GEOMETRY NOW LIVES IN FIVE PLACES
    THAT MUST CHANGE TOGETHER"). HTML/CSS on this site loads nothing external and runs no
    JavaScript (that is what keeps the page alive if GitHub removes the repo), so the six copies
    cannot be replaced by one shared file the way a normal build would do it. This script is the
    substitute: it reads the canonical points out of assets\make-icon.ps1 and asserts every other
    copy still carries the same three points and the same stroke width. A silent mismatch is
    exactly the failure mode that produced the 2026-08-06 refit in the first place.

    Canonical source: assets\make-icon.ps1's $pts array and its Pen width.
    Checked against:
      1. site\index.html    - favicon <link>, URL-encoded data: SVG
      2. site\index.html    - header <svg> (plain markup)
      3. site\holding.html  - favicon <link>, URL-encoded data: SVG
      4. site\holding.html  - header <svg> (plain markup)
      5. assets\FlashDesk-mark-mono.svg - the light-surface single-colour companion

    Run it by double-clicking Check-MarkGeometry.cmd, or:
      powershell -File scripts\Check-MarkGeometry.ps1
    It is also run automatically as section 0 of Check-LiveBuild.ps1, so a divergence is caught
    before anyone is told to download anything.
#>

[CmdletBinding()]
param([string] $RepoRoot)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $RepoRoot = Split-Path -Parent $here
}

$problems = New-Object System.Collections.Generic.List[string]
function Good ($t) { Write-Host "  [ OK ]  $t" -ForegroundColor Green }
function Bad  ($t) { Write-Host "  [FAIL]  $t" -ForegroundColor Red; $problems.Add($t) }

Write-Host ''
Write-Host '================================================================'
Write-Host ' FlashDesk mark - are all six copies of the geometry the same?'
Write-Host '================================================================'
Write-Host ''

# ---- canonical: assets\make-icon.ps1 --------------------------------------------------------
$iconScript = Join-Path $RepoRoot 'assets\make-icon.ps1'
if (-not (Test-Path $iconScript)) { Bad "Canonical source not found: $iconScript"; }
$iconSrc = Get-Content -Raw -Encoding UTF8 -Path $iconScript

$ptMatches = [regex]::Matches($iconSrc, '\(([0-9.]+)\s*\*\s*\$u\),\s*\(([0-9.]+)\s*\*\s*\$u\)\)\)')
if ($ptMatches.Count -lt 3) {
    Bad "Could not find three points in $iconScript - the canonical source itself looks changed shape. Fix this script by hand."
    Write-Host ''; Write-Host 'Cannot continue without a canonical shape.' -ForegroundColor Red
    exit 1
}
$canonPts = $ptMatches | Select-Object -First 3 | ForEach-Object {
    '{0:0.000},{1:0.000}' -f [double]$_.Groups[1].Value, [double]$_.Groups[2].Value
}
$canonPointsStr = $canonPts -join ' '

$strokeMatch = [regex]::Match($iconSrc, 'New-Object System\.Drawing\.Pen\([^,]+,\s*\(([0-9.]+)\s*\*\s*\$u\)\)')
if (-not $strokeMatch.Success) { Bad "Could not find the stroke width in $iconScript." }
$canonStroke = [double]$strokeMatch.Groups[1].Value

Write-Host "Canonical (assets\make-icon.ps1):"
Write-Host "  points = $canonPointsStr"
Write-Host "  stroke width = $canonStroke"
Write-Host ''

function Test-PointsMatch([string]$found, [string]$label) {
    # Normalise: split on whitespace, parse each "x,y" pair to doubles, compare within a tiny
    # tolerance so trailing-zero formatting differences ("2.5" vs "2.500") are not false failures.
    $foundPts = $found -split '\s+' | Where-Object { $_ -match ',' }
    if ($foundPts.Count -ne 3) {
        Bad "$label -- expected 3 points, found $($foundPts.Count): '$found'"
        return
    }
    $ok = $true
    for ($i = 0; $i -lt 3; $i++) {
        $a = $canonPts[$i] -split ','
        $b = $foundPts[$i] -split ','
        if ([Math]::Abs([double]$a[0] - [double]$b[0]) -gt 0.001 -or
            [Math]::Abs([double]$a[1] - [double]$b[1]) -gt 0.001) {
            $ok = $false
        }
    }
    if ($ok) { Good "$label matches the canonical points." }
    else     { Bad "$label DOES NOT MATCH -- found '$found', canonical is '$canonPointsStr'." }
}

function Test-StrokeMatches([double]$found, [string]$label) {
    if ([Math]::Abs($found - $canonStroke) -le 0.001) { Good "$label stroke width matches ($found)." }
    else { Bad "$label stroke width is $found, canonical is $canonStroke." }
}

# ---- site\index.html --------------------------------------------------------------------------
$indexHtml = Join-Path $RepoRoot 'site\index.html'
if (Test-Path $indexHtml) {
    $html = Get-Content -Raw -Encoding UTF8 -Path $indexHtml

    # Favicon: URL-encoded data: URI. %20 = space, %2C would be comma but commas are left literal
    # in this project's encoding (verified by inspection), so decode %20 only before matching.
    $decoded = $html -replace '%20', ' '
    $m = [regex]::Match($decoded, "polyline points='([0-9., ]+)'[^>]*stroke-width='([0-9.]+)'")
    if ($m.Success) {
        Test-PointsMatch $m.Groups[1].Value 'site\index.html favicon (data: URI)'
        Test-StrokeMatches ([double]$m.Groups[2].Value) 'site\index.html favicon (data: URI)'
    } else { Bad "Could not find the favicon polyline in site\index.html." }

    # ⚠ Plain double-quoted markup, ALL of them, not just the first. Until round 4 this page only
    # ever carried ONE such mark (the header). The round-4 (2026-09-04) light redesign puts the mark
    # in the page TWICE — once in the light header (ink-only, no tile) and once in the dark footer
    # (its native tile+green colours) — see site-src\partials\header-v2.html / footer-v2.html. A
    # version of this check that only looked at match [0] would silently stop verifying the
    # footer's copy the moment it was added; checking every match is what keeps that from
    # happening again the way it happened to this file's very first version.
    $headerMatches = [regex]::Matches($html, 'polyline points="([0-9., ]+)"[^>]*stroke-width="([0-9.]+)"')
    if ($headerMatches.Count -ge 1) {
        for ($mi = 0; $mi -lt $headerMatches.Count; $mi++) {
            $label = if ($headerMatches.Count -eq 1) { 'site\index.html header <svg>' } else { "site\index.html mark copy #$($mi + 1) of $($headerMatches.Count)" }
            Test-PointsMatch $headerMatches[$mi].Groups[1].Value $label
            Test-StrokeMatches ([double]$headerMatches[$mi].Groups[2].Value) $label
        }
    } else { Bad "Could not find any plain <svg> polyline in site\index.html." }
} else { Bad "Not found: $indexHtml" }

# ---- site\holding.html ------------------------------------------------------------------------
$holdingHtml = Join-Path $RepoRoot 'site\holding.html'
if (Test-Path $holdingHtml) {
    $html = Get-Content -Raw -Encoding UTF8 -Path $holdingHtml
    $decoded = $html -replace '%20', ' '
    $m = [regex]::Match($decoded, "polyline points='([0-9., ]+)'[^>]*stroke-width='([0-9.]+)'")
    if ($m.Success) {
        Test-PointsMatch $m.Groups[1].Value 'site\holding.html favicon (data: URI)'
        Test-StrokeMatches ([double]$m.Groups[2].Value) 'site\holding.html favicon (data: URI)'
    } else { Bad "Could not find the favicon polyline in site\holding.html." }

    $headerMatches = [regex]::Matches($html, 'polyline points="([0-9., ]+)"[^>]*stroke-width="([0-9.]+)"')
    if ($headerMatches.Count -ge 1) {
        Test-PointsMatch $headerMatches[0].Groups[1].Value 'site\holding.html header <svg>'
        Test-StrokeMatches ([double]$headerMatches[0].Groups[2].Value) 'site\holding.html header <svg>'
    } else { Bad "Could not find the header <svg> polyline in site\holding.html." }
} else { Bad "Not found: $holdingHtml" }

# ---- assets\FlashDesk-mark-mono.svg -------------------------------------------------------------
$monoSvg = Join-Path $RepoRoot 'assets\FlashDesk-mark-mono.svg'
if (Test-Path $monoSvg) {
    $svg = Get-Content -Raw -Encoding UTF8 -Path $monoSvg
    $m = [regex]::Match($svg, 'polyline points="([0-9., ]+)"[^>]*stroke-width="([0-9.]+)"')
    if ($m.Success) {
        Test-PointsMatch $m.Groups[1].Value 'assets\FlashDesk-mark-mono.svg'
        Test-StrokeMatches ([double]$m.Groups[2].Value) 'assets\FlashDesk-mark-mono.svg'
    } else { Bad "Could not find the polyline in assets\FlashDesk-mark-mono.svg." }
} else {
    Write-Host "  [WARN]  assets\FlashDesk-mark-mono.svg not found -- not built yet, or removed." -ForegroundColor Yellow
}

# ---- the two .ico binaries are regenerated FROM make-icon.ps1, not hand-edited -----------------
Write-Host ''
Write-Host 'Not checked here (regenerated, not hand-maintained): assets\FlashDesk.ico and' -ForegroundColor DarkGray
Write-Host 'assets\FlashDesk-live.ico. Re-run assets\make-icon.ps1 after any geometry change and' -ForegroundColor DarkGray
Write-Host 'commit the regenerated files in the same commit.' -ForegroundColor DarkGray

Write-Host ''
Write-Host '================================================================'
if ($problems.Count -eq 0) {
    Write-Host ' MATCH - all copies of the mark carry the same geometry.' -ForegroundColor Green
    Write-Host '================================================================'
    Write-Host ''
    exit 0
} else {
    Write-Host ' MISMATCH - these files have drifted from assets\make-icon.ps1:' -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "   - $p" -ForegroundColor Red }
    Write-Host '================================================================'
    Write-Host ''
    exit 1
}
