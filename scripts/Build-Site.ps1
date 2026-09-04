<#
    Build-Site.ps1 -- generates every page in site\ from site-src\. This is the answer to "the
    header/footer/CSS exist more than once" (Conor, 2026-09-03): rather than hand-maintaining five
    copies of the header and footer and hoping they stay identical -- exactly the mistake that cost
    a five-file hand-edit once already, for the mark's geometry -- there is now only ONE copy of
    each (site-src\partials\header.html, footer.html) and this script stamps it onto every page.
    Drift between pages' headers/footers is no longer possible to introduce by hand; it would
    require editing this script's substitution logic itself.

    WHY A BUILD SCRIPT AND NOT A "CHECK THAT FAILS ON DIVERGENCE" (Conor gave the choice; this is
    the one taken, and here is why): a check can only tell you AFTER the fact that two hand-edited
    copies disagree -- it does not stop the disagreement from being written in the first place, and
    every commit until the check is run risks shipping the drift. Generation makes the drift
    IMPOSSIBLE by construction: there is only one header.html, so there is nothing to diverge from.
    (The mark's geometry still uses the check-based approach, in Check-MarkGeometry.ps1, because
    its five copies are inside five otherwise-independent files this script does not own -- three
    live inside generated HTML anyway and are covered for free once this script runs; the two that
    aren't, make-icon.ps1 and the .ico binaries, are a different kind of artifact a text template
    can't produce. Generation and a failing check are the same idea -- "don't let copies drift" --
    applied wherever each is the cheaper fix.)

    WHAT THIS DOES NOT TOUCH: site\site.css is hand-maintained directly (Conor's own call: "Shared
    CSS in its own file is fine: same origin, same rule respected" -- one shared file already
    satisfies the no-duplication rule without needing a build step). site\inter.woff2,
    site\flashdesk-window.png and site\holding.html are untouched by this script.

    Run it by double-clicking Build-Site.cmd, or: powershell -File scripts\Build-Site.ps1
    Run it EVERY TIME a file under site-src\ changes, before committing -- site\ is the deployed
    output and must never be hand-edited directly (a hand edit there is silently overwritten by the
    next build and will look like it "reverted itself" to whoever finds it later).
#>

[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $SrcDir,
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $RepoRoot = Split-Path -Parent $here
}
if (-not $SrcDir) { $SrcDir = Join-Path $RepoRoot 'site-src' }
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'site' }

Write-Host ''
Write-Host '================================================================'
Write-Host ' FlashDesk site -- building site\ from site-src\'
Write-Host '================================================================'
Write-Host ''

# ------------------------------------------------------------------------------------------------
# TWO LAYOUT TRACKS (added round 4, 2026-09-04). Conor's own build order for the redesign: "design
# the system, then build the HOME PAGE completely in the new language, then ONE inner page... Do
# not roll out the remaining pages until I have seen them." Privacy/FAQ/Terms must render EXACTLY
# as they do today while Home/How-it-works move to the new visual language -- one shared build
# script serving two coexisting designs, selected per PAGE via a `LAYOUT` front-matter key, rather
# than a second script (which would just be this same substitution logic copied once, the exact
# mistake this file exists to prevent). Omit LAYOUT (or set `v1`) to get the original
# layout.html/header.html/footer.html/site.css unchanged; set `LAYOUT: v2` to get
# layout-v2.html/header-v2.html/footer-v2.html/site-v2.css. When the redesign is approved and rolled
# out to the remaining pages, the v1 files retire and this fork collapses back to one track.
$layouts = @{
    v1 = @{
        Layout = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $SrcDir 'layout.html')
        Header = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $SrcDir 'partials\header.html')
        Footer = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $SrcDir 'partials\footer.html')
    }
}
$layoutV2Path = Join-Path $SrcDir 'layout-v2.html'
if (Test-Path $layoutV2Path) {
    $layouts['v2'] = @{
        Layout = Get-Content -Raw -Encoding UTF8 -Path $layoutV2Path
        Header = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $SrcDir 'partials\header-v2.html')
        Footer = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $SrcDir 'partials\footer-v2.html')
    }
}

$pageFiles = Get-ChildItem -Path (Join-Path $SrcDir 'pages') -Filter '*.html' | Sort-Object Name
if ($pageFiles.Count -eq 0) { throw "No page source files found under $SrcDir\pages" }

$built = @()

foreach ($pf in $pageFiles) {
    $raw = Get-Content -Raw -Encoding UTF8 -Path $pf.FullName

    # Front matter: a leading HTML comment <!--PAGE ... --> with one KEY: value per line.
    $fmMatch = [regex]::Match($raw, '^<!--PAGE\s*(.*?)-->', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $fmMatch.Success) {
        throw "No PAGE front matter found at the top of $($pf.Name) -- every page source file must start with <!--PAGE ... -->."
    }
    $fm = @{}
    foreach ($line in ($fmMatch.Groups[1].Value -split "`n")) {
        $line = $line.Trim()
        if ($line -eq '') { continue }
        $kv = $line -split ':\s*', 2
        if ($kv.Count -eq 2) { $fm[$kv[0].Trim()] = $kv[1].Trim() }
    }
    foreach ($required in 'SLUG', 'TITLE', 'DESCRIPTION', 'CANONICAL') {
        if (-not $fm.ContainsKey($required)) {
            throw "$($pf.Name) is missing required front-matter key: $required"
        }
    }

    $content = $raw.Substring($fmMatch.Length).TrimStart("`r", "`n")
    $slug = $fm['SLUG']

    $layoutKey = if ($fm.ContainsKey('LAYOUT')) { $fm['LAYOUT'] } else { 'v1' }
    if (-not $layouts.ContainsKey($layoutKey)) {
        throw "$($pf.Name): LAYOUT '$layoutKey' has no matching layout-$layoutKey.html / partials\header-$layoutKey.html / footer-$layoutKey.html under $SrcDir."
    }
    $layout = $layouts[$layoutKey].Layout
    $headerSrc = $layouts[$layoutKey].Header
    $footerSrc = $layouts[$layoutKey].Footer

    # Stamp aria-current="page" onto the matching nav link, in a fresh copy of each partial --
    # header/footer stay otherwise byte-identical across every page, which is the property that
    # makes them safe to eyeball-diff if anyone ever needs to.
    $navMarker = 'data-nav="' + $slug + '"'
    $header = $headerSrc -replace [regex]::Escape($navMarker), ($navMarker + ' aria-current="page"')
    $footer = $footerSrc -replace [regex]::Escape($navMarker), ($navMarker + ' aria-current="page"')
    if ($header -eq $headerSrc) {
        Write-Host "  [WARN] $($pf.Name): no nav link found for data-nav=""$slug"" -- the header will show no active page." -ForegroundColor Yellow
    }

    $out = $layout
    $out = $out.Replace('{{TITLE}}', $fm['TITLE'])
    $out = $out.Replace('{{DESCRIPTION}}', $fm['DESCRIPTION'])
    $out = $out.Replace('{{CANONICAL}}', $fm['CANONICAL'])
    $out = $out.Replace('{{SLUG}}', $slug)
    $out = $out.Replace('{{HEADER}}', $header)
    $out = $out.Replace('{{FOOTER}}', $footer)
    $out = $out.Replace('{{CONTENT}}', $content)

    if (($out -match '\{\{[A-Z_]+\}\}')) {
        $left = [regex]::Matches($out, '\{\{[A-Z_]+\}\}') | Select-Object -ExpandProperty Value -Unique
        throw "$($pf.Name): unresolved placeholder(s) after substitution: $($left -join ', ')"
    }

    if ($slug -eq 'home') {
        $outPath = Join-Path $OutDir 'index.html'
    } else {
        $outDirForPage = Join-Path $OutDir $slug
        New-Item -ItemType Directory -Force -Path $outDirForPage | Out-Null
        $outPath = Join-Path $outDirForPage 'index.html'
    }

    # No BOM, LF-normalised-by-git-on-checkout is fine -- match how the rest of this repo's text
    # files are written (Set-Content -Encoding UTF8 in this PowerShell version omits the BOM only
    # via the explicit .NET writer below; PowerShell's own -Encoding UTF8 adds one, which the
    # existing site pages do not carry, so write it the same way make-icon.ps1's siblings do).
    [System.IO.File]::WriteAllText($outPath, $out, (New-Object System.Text.UTF8Encoding($false)))
    $relOut = $outPath.Substring($RepoRoot.Length).TrimStart('\')
    Write-Host "  built $relOut  (from site-src\pages\$($pf.Name), layout $layoutKey)"
    $built += [pscustomobject]@{ Slug = $slug; Layout = $layoutKey; Path = $outPath; RelPath = $relOut }
}

Write-Host ''
Write-Host "Built $($built.Count) page(s)." -ForegroundColor Green
Write-Host ''
$built | Format-Table Slug, Layout, RelPath -AutoSize
