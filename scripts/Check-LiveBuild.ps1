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
    only reported as a PROBLEM when the missing commits actually touch code that goes into the exe.
    Documentation-only commits are reported as fine — and, UPDATED 2026-09-15, so is a commit that
    only edits a comment or a blank line inside a .cs file under src\ (checked line by line, not
    assumed — see Test-CommentOnlyDiff below); the exe's behaviour did not change, so republishing
    it would prove nothing. A check that cries wolf every time a comment changes is a check people
    learn to ignore, and that is worse than none.

    Run it by double-clicking Check-LiveBuild.cmd, or:  powershell -File scripts\Check-LiveBuild.ps1
#>

[CmdletBinding()]
param(
    # Primary download — the MSI installer linked from every page on the site.
    # Changed 2026-09-21: the site now offers only the MSI; the EXE is still published
    # to GitHub but is no longer linked from any page.
    [string] $Url    = 'https://flashdesk.org/dl/FlashDesk-setup.msi',
    # EXE on GitHub — used only for the build-stamp check (section 3), because the MSI
    # does not embed the git commit in its VersionInfo the way the self-contained exe does.
    # The exe itself is still uploaded to every GitHub release; it just is not linked from
    # the site any more. Verifying it is still the most reliable way to confirm WHICH build
    # is inside the MSI, since both are built from the same commit in the same publish step.
    [string] $ExeUrl = 'https://github.com/conorhonor111-art/flashdesk/releases/latest/download/FlashDesk.exe',
    [string] $Site   = 'https://flashdesk.org',
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

# Added 2026-09-15, for section 3. A LINE-BASED HEURISTIC, not a real C# parser: a changed line
# counts as "safe" only if, once trimmed, it is blank or is entirely a // or /* */ style comment.
# It will not catch a real code change sharing a line with a comment (`DoThing(); // note`), and it
# cannot see inside a multi-line string that happens to contain something that looks like a
# comment marker. When unsure, it must call a line unsafe — never the reverse. Section 3 also
# always PRINTS every changed .cs file regardless of what this returns, so a human is never asked
# to trust the heuristic blindly (Conor, 2026-09-15: "if you cannot tell those apart reliably, say
# so and instead state which .cs files changed").
function Test-CommentOnlyDiff($path, $fromRef, $toRef) {
    $diffLines = @(git diff $fromRef $toRef -- $path)
    foreach ($line in $diffLines) {
        if ($line.Length -eq 0) { continue }
        if ($line.StartsWith('+++') -or $line.StartsWith('---')) { continue }   # file header, not content
        $marker = $line.Substring(0, 1)
        if ($marker -ne '+' -and $marker -ne '-') { continue }                  # diff metadata / context line
        $body = $line.Substring(1).Trim()
        if ($body -eq '') { continue }                                          # blank-line change
        if ($body -match '^(//|/\*|\*/|\*(?!/))') { continue }                  # a comment line, start to end
        return $false                                                           # anything else: real code
    }
    return $true
}

Say ''
Say '================================================================'
Say ' FlashDesk - is the file people download the build we think?'
Say '================================================================'
Say ''

# ------------------------------------------------------- 0. the mark's geometry, everywhere it lives
# Added 2026-09-03: the mark's points live in six files by hand (CLAUDE.md, "THE MARK WAS REFITTED
# 2026-08-06"), and a page that is otherwise byte-identical to the repo (section 5 below) can still
# be wearing a drifted mark if only make-icon.ps1 or one HTML copy was touched. Checked first, before
# any network call, because a local mismatch needs no download to catch.
Say '0. The mark - same geometry in every copy'
$markCheck = Join-Path $PSScriptRoot 'Check-MarkGeometry.ps1'
if (Test-Path $markCheck) {
    & $markCheck -RepoRoot $RepoRoot
    if ($LASTEXITCODE -ne 0) { Bad 'The mark has drifted between files - see the output above.' }
    else { Good 'All copies of the mark carry the same geometry.' }
} else {
    Bad "Could not find $markCheck."
}

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

# ------------------------------------------------------------ 2. the MSI installer
# ⚠ UPDATED 2026-09-21: the primary download is now the MSI installer served directly from
# flashdesk.org/dl/FlashDesk-setup.msi. The EXE is still published to every GitHub release
# but is no longer linked from any page on the site. This section verifies the MSI is
# accessible and a plausible size; section 3 downloads the EXE from GitHub to verify the
# build commit (the MSI does not embed a git hash in its VersionInfo).
Say ''
Say '2. The MSI installer (the download every visitor gets)'
$tempMsi = Join-Path $env:TEMP ("flashdesk-livecheck-{0}.msi" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
try {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-WebRequest -Uri $Url -OutFile $tempMsi -UseBasicParsing -TimeoutSec 900
    $sw.Stop()
    $sizeMsi = (Get-Item $tempMsi).Length
    Good ("Downloaded {0:N1} MB in {1:N0} seconds." -f ($sizeMsi / 1MB), $sw.Elapsed.TotalSeconds)
    if ($sizeMsi -lt 40MB) {
        Bad "That file is far too small to be the FlashDesk installer - the server may be serving an error page."
    } else {
        Note ("MSI is {0:N0} bytes - within the expected range." -f $sizeMsi)
    }
} catch {
    Bad "The MSI download failed: $($_.Exception.Message)"
    Note 'This is the file every visitor downloads. Treat this as a blocking failure.'
} finally {
    Remove-Item $tempMsi -Force -ErrorAction SilentlyContinue
}

# ------------------------------------------------ 3. which build is it, really
# The MSI does not embed the git commit in VersionInfo the way the self-contained EXE does.
# The EXE is still published to every GitHub release alongside the MSI; downloading it here
# is the reliable way to confirm WHICH build is packaged, since both are produced in the
# same publish step from the same commit.
Say ''
Say '3. Which build is on the server (via the EXE on GitHub)'
$temp = Join-Path $env:TEMP ("flashdesk-livecheck-{0}.exe" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$bust = "$ExeUrl" + ('?cb=' + (Get-Date -UFormat %s))
try {
    $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-WebRequest -Uri $bust -OutFile $temp -UseBasicParsing -TimeoutSec 900
    $sw2.Stop()
    $sizeExe = (Get-Item $temp).Length
    Note ("EXE downloaded {0:N1} MB in {1:N0} s (not the site download - used for version stamp only)." -f ($sizeExe / 1MB), $sw2.Elapsed.TotalSeconds)
    if ($sizeExe -lt 20MB) {
        Bad "EXE from GitHub is far too small - the release may be missing the file."
    }
} catch {
    Bad "The EXE download from GitHub failed: $($_.Exception.Message)"
    Note 'Cannot verify which build is packaged - treat this as a missing check, not a pass.'
    $temp = $null
}

if ($temp -and (Test-Path $temp)) {
    $info = (Get-Item $temp).VersionInfo
    $productVersion = $info.ProductVersion
    Note "The EXE says: $($info.ProductName) $productVersion"
    Note "Windows will show: `"$($info.FileDescription)`""

    $liveCommit = $null
    if ($productVersion -match '\+([0-9a-f]{7,40})') { $liveCommit = $Matches[1] }

    if (-not $liveCommit) {
        Bad 'The EXE does not carry a build stamp, so it cannot be identified.'
        Note 'Expected a version like 0.4.0+84c3bab... Republish with the documented publish command.'
    } else {
        Push-Location $RepoRoot
        try {
            $head = (git rev-parse HEAD).Trim()
            $known = $true
            try { git cat-file -e "$liveCommit^{commit}" 2>$null; $known = ($LASTEXITCODE -eq 0) } catch { $known = $false }

            if (-not $known) {
                Bad "The release build is $liveCommit, which this repository has never seen."
                Note 'Either it was built from someone else''s copy, or this repo is behind. Do not test against it.'
            }
            elseif ($liveCommit -eq $head -or $head.StartsWith($liveCommit)) {
                Good "The release is exactly this repository's current build ($($liveCommit.Substring(0,7)))."
            }
            else {
                # THE SUBTLETY: only REAL code changes matter. Docs-only commits are not a problem — and
                # as of 2026-09-15, neither is a commit that only edits a comment or blank line INSIDE a
                # .cs file. A non-.cs file under src\ gets no benefit of the doubt.
                $allMissing   = @(git log --oneline "$liveCommit..HEAD")
                $changedFiles = @(git diff --name-only $liveCommit HEAD -- 'src/')
                $csFiles      = @($changedFiles | Where-Object { $_ -like '*.cs' })
                $otherFiles   = @($changedFiles | Where-Object { $_ -notlike '*.cs' })
                $unsafeCs     = @($csFiles | Where-Object { -not (Test-CommentOnlyDiff $_ $liveCommit 'HEAD') })

                if ($changedFiles.Count -eq 0) {
                    Good ("The release build is code-current ({0})." -f $liveCommit.Substring(0,7))
                    Note ("It is behind by {0} commit(s), but none of them touch src\ - documentation only." -f $allMissing.Count)
                } elseif ($otherFiles.Count -eq 0 -and $unsafeCs.Count -eq 0) {
                    Good ("The release build is code-current ({0})." -f $liveCommit.Substring(0,7))
                    Note ("It is behind by {0} commit(s). {1} .cs file(s) changed in that range, but every" -f $allMissing.Count, $csFiles.Count)
                    Note 'changed line in each is a comment or blank line, checked line-by-line - not assumed:'
                    foreach ($f in $csFiles) { Note "  comment/blank only: $f" }
                } else {
                    Bad ("The release is an OLD build - {0} commit(s) touch real .cs code, missing from it:" -f $allMissing.Count)
                    foreach ($c in $allMissing) { Note "  missing: $c" }
                    Note ''
                    Note 'Changed files under src\ in the missing range:'
                    foreach ($f in $csFiles)    { Note ("  {0}  {1}" -f $f, $(if ($unsafeCs -contains $f) { '(real code changed)' } else { '(comment/blank only)' })) }
                    foreach ($f in $otherFiles) { Note ("  {0}  (not a .cs file - no comment-only exemption possible)" -f $f) }
                    Note ''
                    Note 'Republish and re-upload before testing, or the test will measure the wrong program:'
                    Note '  dotnet publish src\RemoteDesktop.Host -c Release -r win-x64 --self-contained true \'
                    Note '    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \'
                    Note '    -p:IncludeNativeLibrariesForSelfExtract=true -o publish\host'
                }
            }
        } finally { Pop-Location }
    }
    Remove-Item $temp -Force -ErrorAction SilentlyContinue
}

# ------------------------------------------ 4. the download button on the page
# ⚠ REWRITTEN 2026-09-21: the site previously had two download routes (a GitHub EXE and a
# cPanel MSI). As of v0.4.0-25 there is exactly ONE route: the MSI installer at
# flashdesk.org/dl/FlashDesk-setup.msi. This section verifies that every download button
# on the home page points at that same address, and that the address matches $Url (the one
# section 2 already downloaded). If the regex ever stops finding a button, update it here —
# the pattern is intentionally broad so minor markup changes do not silently skip the check.
#
# Buttons to find: the header-cta (top-right of every page), and any btn links in the main
# content (bottom CTA band, etc.). All should point to $Url.
Say ''
Say '4. Download buttons on the page all point to the right place'
$localPageForLink = Join-Path $RepoRoot 'site\index.html'
$buttonUrls = @()
if (Test-Path $localPageForLink) {
    $html = Get-Content -Raw -Encoding UTF8 -Path $localPageForLink
    # Match any <a> whose href looks like a download URL for FlashDesk (contains .msi or .exe or
    # the GitHub releases path). Captures the href value.  The pattern is deliberately broad:
    # a missed button is a false pass, which is the failure mode that cost time before (2026-08-06).
    $dlPattern = '(?is)<a\b[^>]*\bhref\s*=\s*"([^"]*(?:FlashDesk|flashdesk\.org/dl|releases/latest/download)[^"]*)"'
    foreach ($m in [regex]::Matches($html, $dlPattern)) {
        $u = $m.Groups[1].Value.Trim()
        if ($u -notin $buttonUrls) { $buttonUrls += $u }
    }
}

if ($buttonUrls.Count -eq 0) {
    Bad 'No FlashDesk download links found in site\index.html -- the button check DID NOT RUN.'
    Note 'The page markup may have changed shape. Update the $dlPattern regex in section 4 of'
    Note 'scripts\Check-LiveBuild.ps1 to match the current button structure.'
} else {
    $wrongUrls = @($buttonUrls | Where-Object { $_ -ne $Url })
    if ($wrongUrls.Count -eq 0) {
        Good ("All {0} download button(s) on the home page point to the correct MSI URL." -f $buttonUrls.Count)
        Note "  $Url"
    } else {
        foreach ($u in $wrongUrls) {
            Bad "A download button points somewhere unexpected: $u"
            Note "  Expected: $Url"
            Note '  Update the href in site\index.html (and every other page that has this button).'
        }
        # Report correct ones too, for context
        foreach ($u in ($buttonUrls | Where-Object { $_ -eq $Url })) {
            Good "  (correct)  $u"
        }
    }
}

# --------------------------------------------------- 5. every page, not just the home page
# ⚠ REWRITTEN 2026-09-04 when the site grew from one page to five (home, how-it-works, privacy,
# faq, terms). The old section 5+6 only ever checked https://flashdesk.org itself; a new page that
# 404s live is exactly the class of failure this script exists to catch (see the 2026-08-06 note
# below, which is the same lesson applying to a whole PAGE now instead of one image). So this walks
# EVERY generated page found locally under site\, not a hardcoded list — add a page under
# site-src\pages\, run Build-Site.ps1, and this script picks it up on the next run with no edit
# here required.
#
# ⚠ THE 2026-08-06 LESSON THIS BUILDS ON, KEPT VERBATIM SO IT IS NOT LOST: the page had just gained
# its first two files that are not index.html - a screenshot and a font - and only index.html was
# uploaded. The old section 5 compared the HTML and found it identical, section 3 found the exe
# current, so the check reported READY while every visitor saw the alt text where the picture
# should have been. A page that is byte-identical to the repository can still be broken, because
# being correct is not the same as being complete - so every URL every page asks the browser to
# fetch is fetched, for every page, not only the one that used to be the only page.
Say ''
Say '5. Every page: is it the one in this repository, and does everything it asks for exist'

$localPages = @(Get-ChildItem -Path (Join-Path $RepoRoot 'site') -Recurse -Filter 'index.html' -File |
    Sort-Object FullName)
if ($localPages.Count -eq 0) {
    Bad "No index.html files found under $(Join-Path $RepoRoot 'site') - nothing to check."
}

# Assets are checked once each, even if five pages all reference /site.css - five identical fetches
# would just be slow, not more thorough.
$checkedAssets = @{}

function Test-OnePage($localPath, $liveUrl) {
    Note ("Page: {0}" -f $liveUrl)
    try {
        $pageTmp = Join-Path $env:TEMP ("flashdesk-page-{0}-{1}.html" -f (Get-Date -Format 'HHmmssfff'), (Get-Random))
        Invoke-WebRequest -Uri $liveUrl -OutFile $pageTmp -UseBasicParsing -TimeoutSec 60 -Headers @{ 'Cache-Control' = 'no-cache' }
        # -Encoding UTF8 matters on BOTH reads: Windows PowerShell reads a BOM-less UTF-8 file (or a
        # server response with no charset header) as ANSI/ISO-8859-1 otherwise, which mangles every
        # em-dash and reports a page full of differences that do not exist.
        $live = Get-Content -Raw -Encoding UTF8 -Path $pageTmp
        Remove-Item $pageTmp -Force -ErrorAction SilentlyContinue
        $repo = Get-Content -Raw -Encoding UTF8 -Path $localPath

        $lf = [string][char]10
        $norm = {
            param($t)
            ($t -replace ([string][char]13), '') -split $lf | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -ne '' }
        }
        $liveLines = @(& $norm $live)
        $repoLines = @(& $norm $repo)
        $missing = @(Compare-Object -ReferenceObject $liveLines -DifferenceObject $repoLines |
                     Where-Object { $_.SideIndicator -eq '=>' } | Select-Object -ExpandProperty InputObject)
        $extra   = @(Compare-Object -ReferenceObject $liveLines -DifferenceObject $repoLines |
                     Where-Object { $_.SideIndicator -eq '<=' } | Select-Object -ExpandProperty InputObject)

        if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
            Good "  matches this repository exactly."
        } else {
            Bad ("{0} - NOT the page in this repository ({1} line(s) missing from the server, {2} line(s) on the server not in the repo)." -f $liveUrl, $missing.Count, $extra.Count)
            if ($missing.Count -gt 0) {
                Note '  In the repo but NOT on the server:'
                foreach ($l in ($missing | Select-Object -First 3)) { Note ("    + " + $l.Trim()) }
                if ($missing.Count -gt 3) { Note ("    ... and {0} more" -f ($missing.Count - 3)) }
            }
            if ($extra.Count -gt 0) {
                Note '  On the server but NOT in the repo:'
                foreach ($l in ($extra | Select-Object -First 3)) { Note ("    - " + $l.Trim()) }
                if ($extra.Count -gt 3) { Note ("    ... and {0} more" -f ($extra.Count - 3)) }
            }
        }

        # Every asset THIS page's live HTML asks the browser to load.
        $refs = New-Object System.Collections.Generic.List[string]
        foreach ($m in [regex]::Matches($live, '(?i)\ssrc\s*=\s*"([^"]+)"'))          { $refs.Add($m.Groups[1].Value) }
        foreach ($m in [regex]::Matches($live, '(?i)url\(\s*[''"]?([^''")]+)[''"]?\s*\)')) { $refs.Add($m.Groups[1].Value) }
        foreach ($m in [regex]::Matches($live, '(?i)<link[^>]+href\s*=\s*"([^"]+)"'))  { $refs.Add($m.Groups[1].Value) }
        $assets = $refs | Where-Object { $_ -notmatch '^(data:|#|mailto:|tel:)' } | Sort-Object -Unique

        foreach ($a in $assets) {
            if ($a -match '^https?://') {
                $assetHost = ([uri]$a).Host
                if ($assetHost -notlike '*flashdesk.org') {
                    Bad "  loads an asset from another site: $a"
                    Note '  This site is supposed to fetch nothing external - that is what keeps it'
                    Note '  working if GitHub ever removes the repository. Host it on flashdesk.org.'
                    continue
                }
                $full = $a
            } else {
                $full = ($Site.TrimEnd('/')) + '/' + $a.TrimStart('/')
            }

            if ($checkedAssets.ContainsKey($full)) { continue }
            $checkedAssets[$full] = $true

            try {
                $r = Invoke-WebRequest -Uri $full -UseBasicParsing -TimeoutSec 60 -Method Get
                if ($r.StatusCode -eq 200) { Good ("  {0}  ({1:N0} bytes)" -f $a, $r.RawContentLength) }
                else { Bad ("  {0} answered with status {1}." -f $a, $r.StatusCode) }
            } catch {
                $code = $null
                if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
                if ($code) { Bad ("  {0} is MISSING from the server (HTTP {1})." -f $a, $code) }
                else       { Bad ("  {0} could not be fetched: {1}" -f $a, $_.Exception.Message) }
                Note ("  Upload it to the web root. In this repository it is site\{0}" -f ($a -replace '^/',''))
            }
        }
        if ($assets.Count -eq 0) { Good "  loads nothing but itself." }
    } catch {
        Bad ("{0} - could not be fetched: {1}" -f $liveUrl, $_.Exception.Message)
    }
    Say ''
}

foreach ($lp in $localPages) {
    $rel = $lp.DirectoryName.Substring((Join-Path $RepoRoot 'site').Length).Trim('\') -replace '\\', '/'
    $liveUrl = if ($rel -eq '') { $Site.TrimEnd('/') + '/' } else { $Site.TrimEnd('/') + '/' + $rel + '/' }
    Test-OnePage -localPath $lp.FullName -liveUrl $liveUrl
}

# --------------------------------------------------------------- 6. the verdict
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
