<#
    Check-LiveBuild.ps1 — the gate to run BEFORE any test, and before telling anyone to download.

    WHY THIS EXISTS. On 2026-08-04 the site was perfect, the certificate was valid, the download
    worked, and the file being served was TWELVE COMMITS OLD — built before adaptive quality
    existed. Every visible check passed while the thing people actually download was missing the
    feature the whole slow-connection story depends on. A test run against it would have "failed"
    and we would have spent a week fixing working code.

    So the exe carries the git commit it was built from, and this script reads it back off the
    LIVE download and compares it with this repository.

    ONE DELIBERATE SUBTLETY, and it is what makes this worth trusting: a difference from HEAD is
    only reported as a PROBLEM when the missing commits actually touch code that goes into the exe
    (anything under src\). Documentation-only commits are reported as fine. A check that cries wolf
    every time a comment changes is a check people learn to ignore, and that is worse than none.

    Run it by double-clicking Check-LiveBuild.cmd, or:  powershell -File scripts\Check-LiveBuild.ps1
#>

[CmdletBinding()]
param(
    [string] $Url  = 'https://flashdesk.org/dl/FlashDesk.exe',
    [string] $Site = 'https://flashdesk.org',
    [string] $RepoRoot
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell redraws a progress bar for every chunk of a download, which turned a 6-second
# transfer into 264 seconds when this was first run. A check that takes four minutes is a check that
# gets skipped, so the progress bar goes.
$ProgressPreference = 'SilentlyContinue'

# Worked out in the body, not in param(): $PSScriptRoot is not reliably populated while parameter
# defaults are being bound, which made the script fail before it did anything useful.
if (-not $RepoRoot) {
    $here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $RepoRoot = Split-Path -Parent $here
}
$problems = New-Object System.Collections.Generic.List[string]

function Say  ($t) { Write-Host $t }
function Good ($t) { Write-Host "  [ OK ]  $t" -ForegroundColor Green }
function Bad  ($t) { Write-Host "  [FAIL]  $t" -ForegroundColor Red; $problems.Add($t) }
function Note ($t) { Write-Host "          $t" -ForegroundColor DarkGray }

Say ''
Say '================================================================'
Say ' FlashDesk - is the file people download the build we think?'
Say '================================================================'
Say ''

# ---------------------------------------------------------------- 1. the site
Say '1. The website'
try {
    $page = Invoke-WebRequest -Uri $Site -UseBasicParsing -TimeoutSec 30
    if ($page.StatusCode -eq 200) { Good "$Site answers, and its certificate is trusted." }
    else { Bad "$Site answered with status $($page.StatusCode)." }
} catch {
    Bad "$Site could not be loaded: $($_.Exception.Message)"
    Note 'A certificate error shows up here. Nothing else below can be trusted until this passes.'
}

# ------------------------------------------------------------ 2. the download
Say ''
Say '2. The download'
$temp = Join-Path $env:TEMP ("flashdesk-livecheck-{0}.exe" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$bust = "$Url" + ('?cb=' + (Get-Date -UFormat %s))
try {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-WebRequest -Uri $bust -OutFile $temp -UseBasicParsing -TimeoutSec 900
    $sw.Stop()
    $size = (Get-Item $temp).Length
    Good ("Downloaded {0:N1} MB in {1:N0} seconds." -f ($size / 1MB), $sw.Elapsed.TotalSeconds)
    if ($size -lt 20MB) {
        Bad "That file is far too small to be FlashDesk - the server may be serving an error page."
    }
} catch {
    Bad "The download failed: $($_.Exception.Message)"
    Say ''
    Say 'Stopping here - there is nothing to check.'
    exit 1
}

# ------------------------------------------------ 3. which build is it, really
Say ''
Say '3. Which build is on the server'
$info = (Get-Item $temp).VersionInfo
$productVersion = $info.ProductVersion
Note "The file says: $($info.ProductName) $productVersion"
Note "Windows will show: `"$($info.FileDescription)`""

$liveCommit = $null
if ($productVersion -match '\+([0-9a-f]{7,40})') { $liveCommit = $Matches[1] }

if (-not $liveCommit) {
    Bad 'That file does not carry a build stamp, so it cannot be identified.'
    Note 'Expected a version like 0.3.0+84c3bab... Republish with the documented publish command.'
} else {
    Push-Location $RepoRoot
    try {
        $head = (git rev-parse HEAD).Trim()
        $known = $true
        try { git cat-file -e "$liveCommit^{commit}" 2>$null; $known = ($LASTEXITCODE -eq 0) } catch { $known = $false }

        if (-not $known) {
            Bad "The server is serving build $liveCommit, which this repository has never seen."
            Note 'Either it was built from someone else''s copy, or this repo is behind. Do not test against it.'
        }
        elseif ($liveCommit -eq $head -or $head.StartsWith($liveCommit)) {
            Good "The server is serving exactly this repository's current build ($($liveCommit.Substring(0,7)))."
        }
        else {
            # THE SUBTLETY: only code changes matter. Docs-only commits are not a problem.
            $codeMissing = @(git log --oneline "$liveCommit..HEAD" -- 'src/')
            $allMissing  = @(git log --oneline "$liveCommit..HEAD")

            if ($codeMissing.Count -eq 0) {
                Good ("The server's build is code-current ({0})." -f $liveCommit.Substring(0,7))
                Note ("It is behind by {0} commit(s), but none of them touch src\ - documentation only." -f $allMissing.Count)
            } else {
                Bad ("The server is serving an OLD build - {0} code change(s) are missing from it:" -f $codeMissing.Count)
                foreach ($c in $codeMissing) { Note "  missing: $c" }
                Note ''
                Note 'Republish and re-upload before testing, or the test will measure the wrong program:'
                Note '  dotnet publish src\RemoteDesktop.Host -c Release -r win-x64 --self-contained true \'
                Note '    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \'
                Note '    -p:IncludeNativeLibrariesForSelfExtract=true -o C:\Users\PC\Desktop\flashdesk-upload'
            }
        }
    } finally { Pop-Location }
}

# -------------------------------------------- 4. the OTHER download route, the one people use
# Added 2026-08-06. Until now this script only checked https://flashdesk.org/dl/FlashDesk.exe — and
# that is the FALLBACK. The green button on the page points at a GitHub release, because the
# identical file served from flashdesk.org was blocked by Chrome (measured 2026-08-05). So the route
# almost every stranger takes was the one route never verified, and a READY here would have meant
# nothing about it. The link is read OFF THE PAGE rather than hard-coded, so this follows the button
# wherever it points.
Say ''
Say '4. Both download routes serve the same file'
$localPageForLink = Join-Path $RepoRoot 'site\index.html'
$buttonUrl = $null
if (Test-Path $localPageForLink) {
    $html = Get-Content -Raw -Encoding UTF8 -Path $localPageForLink
    if ($html -match 'class="download"\s+href="([^"]+)"') { $buttonUrl = $Matches[1] }
}

if (-not $buttonUrl) {
    Bad 'Could not find the download button''s address in site\index.html, so the main route was not checked.'
} elseif ($buttonUrl -eq $Url) {
    Good 'The page''s button points at the same address this check already downloaded.'
} else {
    Note "The button points at: $buttonUrl"
    $temp2 = Join-Path $env:TEMP ("flashdesk-livecheck-button-{0}.exe" -f (Get-Date -Format 'HHmmss'))
    try {
        Invoke-WebRequest -Uri $buttonUrl -OutFile $temp2 -UseBasicParsing -TimeoutSec 900
        $sizeA = (Get-Item $temp).Length
        $sizeB = (Get-Item $temp2).Length
        $hashA = (Get-FileHash -Path $temp  -Algorithm SHA256).Hash
        $hashB = (Get-FileHash -Path $temp2 -Algorithm SHA256).Hash
        Note ("flashdesk.org/dl : {0:N0} bytes  {1}" -f $sizeA, $hashA.Substring(0, 16) + '...')
        Note ("the green button : {0:N0} bytes  {1}" -f $sizeB, $hashB.Substring(0, 16) + '...')
        if ($hashA -eq $hashB) {
            Good 'Both routes serve byte-for-byte the same file.'
        } else {
            Bad 'THE TWO DOWNLOAD ROUTES SERVE DIFFERENT FILES.'
            Note 'Whichever is older must be replaced. The button is what strangers actually click,'
            Note 'so if only one can be fixed now, fix that one first.'
        }
    } catch {
        Bad "The button's download could not be fetched: $($_.Exception.Message)"
        Note 'That is the link every visitor clicks. Treat this as more serious than the fallback failing.'
    } finally {
        Remove-Item $temp2 -Force -ErrorAction SilentlyContinue
    }
}

# The live page's HTML, kept for the asset sweep in section 6.
$livePage = $null

# ------------------------------------------------------- 5. is the PAGE current?
# Added after being caught a SECOND time by the same class of problem: the exe was current and the
# page was not, so the site told people the download was "about 65 MB" when it was 68.5. Everything
# a stranger reads lives on that page, so a stale page is as bad as a stale file.
Say ''
Say '5. Is the download page the one in this repository'
$localPage = Join-Path $RepoRoot 'site\index.html'
if (-not (Test-Path $localPage)) {
    Bad "Cannot find $localPage to compare against."
} else {
    try {
        # Fetched to a file and read back as UTF-8 rather than using .Content: when the server sends
        # no charset, Windows PowerShell decodes the body as ISO-8859-1, which mangles every em-dash
        # and reported a page full of differences that did not exist.
        $pageTmp = Join-Path $env:TEMP ("flashdesk-page-{0}.html" -f (Get-Date -Format 'HHmmss'))
        Invoke-WebRequest -Uri $Site -OutFile $pageTmp -UseBasicParsing -TimeoutSec 60 -Headers @{ 'Cache-Control' = 'no-cache' }
        $livePage = Get-Content -Raw -Encoding UTF8 -Path $pageTmp
        Remove-Item $pageTmp -Force -ErrorAction SilentlyContinue
        # -Encoding UTF8 matters: Windows PowerShell reads a BOM-less UTF-8 file as ANSI, which turned
        # every em-dash into a different string on one side and produced a page full of imaginary
        # differences the first time this ran.
        $repoPage = Get-Content -Raw -Encoding UTF8 -Path $localPage

        # Compared after normalising line endings and trailing spaces: a web server may serve either,
        # and that difference is not staleness.
        $lf = [string][char]10
        $norm = {
            param($t)
            ($t -replace ([string][char]13), '') -split $lf | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -ne '' }
        }
        $liveLines = @(& $norm $livePage)
        $repoLines = @(& $norm $repoPage)

        $missing = @(Compare-Object -ReferenceObject $liveLines -DifferenceObject $repoLines |
                     Where-Object { $_.SideIndicator -eq '=>' } | Select-Object -ExpandProperty InputObject)
        $extra   = @(Compare-Object -ReferenceObject $liveLines -DifferenceObject $repoLines |
                     Where-Object { $_.SideIndicator -eq '<=' } | Select-Object -ExpandProperty InputObject)

        if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
            Good 'The live page is exactly the one in this repository.'
        } else {
            Bad ("The live page is NOT the one in this repository - {0} line(s) missing from the server, {1} line(s) on the server that are not in the repo." -f $missing.Count, $extra.Count)
            if ($missing.Count -gt 0) {
                Note 'In the repo but NOT on the server (these changes are not live):'
                foreach ($l in ($missing | Select-Object -First 5)) { Note ("  + " + $l.Trim()) }
                if ($missing.Count -gt 5) { Note ("  ... and {0} more" -f ($missing.Count - 5)) }
            }
            if ($extra.Count -gt 0) {
                Note 'On the server but NOT in the repo (the server has older or hand-edited text):'
                foreach ($l in ($extra | Select-Object -First 5)) { Note ("  - " + $l.Trim()) }
                if ($extra.Count -gt 5) { Note ("  ... and {0} more" -f ($extra.Count - 5)) }
            }
            Note ''
            Note ("Fix: upload {0} to the web root as index.html." -f $localPage)
        }
    } catch {
        Bad "Could not fetch the live page to compare: $($_.Exception.Message)"
    }
}

# -------------------------------------------- 6. does everything the page asks for actually exist?
# ⚠ ADDED 2026-08-06, AFTER THIS SCRIPT SAID "READY" WHILE THE PAGE WAS VISIBLY BROKEN.
# The page had just gained its first two files that are not index.html - a screenshot and a font -
# and only index.html was uploaded. Section 5 compared the HTML and found it identical, section 3
# found the exe current, so the check reported READY while every visitor saw the alt text
# "The FlashDesk window: a large 9-digit number..." where the picture should have been.
#
# THE LESSON, and it is the same one this whole script exists for: a page that is byte-identical to
# the repository can still be broken, because being correct is not the same as being complete. So
# now every URL the page asks the browser to fetch is fetched.
Say ''
Say '6. Everything the page asks the browser to load'
if (-not $livePage) {
    Bad 'The live page was not readable, so its images and fonts could not be checked.'
} else {
    $refs = New-Object System.Collections.Generic.List[string]
    foreach ($m in [regex]::Matches($livePage, '(?i)\ssrc\s*=\s*"([^"]+)"'))          { $refs.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($livePage, '(?i)url\(\s*[''"]?([^''")]+)[''"]?\s*\)')) { $refs.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($livePage, '(?i)<link[^>]+href\s*=\s*"([^"]+)"'))  { $refs.Add($m.Groups[1].Value) }

    # data: URIs are already inside the page, and #anchors and mail links fetch nothing.
    $assets = $refs | Where-Object { $_ -notmatch '^(data:|#|mailto:|tel:)' } | Sort-Object -Unique

    if ($assets.Count -eq 0) {
        Good 'The page loads nothing but itself.'
    }
    foreach ($a in $assets) {
        if ($a -match '^https?://') {
            $assetHost = ([uri]$a).Host
            if ($assetHost -notlike '*flashdesk.org') {
                # CLAUDE.md records that this page fetches NOTHING from anywhere else, which is what
                # makes it survive a GitHub takedown. An external asset silently ends that property.
                Bad "The page loads an asset from another site: $a"
                Note 'This page is supposed to fetch nothing external - that is what keeps it working'
                Note 'if GitHub ever removes the repository. Host the file on flashdesk.org instead.'
                continue
            }
            $full = $a
        } else {
            $full = ($Site.TrimEnd('/')) + '/' + $a.TrimStart('/')
        }

        try {
            $r = Invoke-WebRequest -Uri $full -UseBasicParsing -TimeoutSec 60 -Method Get
            if ($r.StatusCode -eq 200) {
                Good ("{0}  ({1:N0} bytes)" -f $a, $r.RawContentLength)
            } else {
                Bad ("{0} answered with status {1}." -f $a, $r.StatusCode)
            }
        } catch {
            $code = $null
            if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
            if ($code) { Bad ("{0} is MISSING from the server (HTTP {1})." -f $a, $code) }
            else       { Bad ("{0} could not be fetched: {1}" -f $a, $_.Exception.Message) }
            Note ("Upload it to the same folder as index.html. In this repository it is site\{0}" -f ($a -replace '^/',''))
        }
    }
}

# --------------------------------------------------------------- 7. the verdict
Remove-Item $temp -Force -ErrorAction SilentlyContinue
Say ''
Say '================================================================'
if ($problems.Count -eq 0) {
    Write-Host ' READY - the file people download is the build you expect.' -ForegroundColor Green
    Say '================================================================'
    Say ''
    exit 0
} else {
    Write-Host ' NOT READY - do not run a test or send anyone the link yet:' -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "   - $p" -ForegroundColor Red }
    Say '================================================================'
    Say ''
    exit 1
}
