<#
    Copy-Summary.ps1 — put last-summary.md on the clipboard, and prove it got there.

    WHY A SCRIPT AND NOT A ONE-LINER. Embedding multi-line text, or even the escape sequences
    needed to compare it, inside a shell command kept failing on quoting. Reading from a FILE and
    comparing with no escapes in sight is the version that works every time.

    WHY IT VERIFIES. Set-Clipboard from a non-interactive shell does not always take. Reporting
    "copied" without checking would be a claim, not a fact — and a summary that silently failed to
    copy is worse than none, because it is trusted.
#>

[CmdletBinding()]
param(
    [string] $Path
)

$ErrorActionPreference = 'Stop'

# Worked out in the body, not in param(): PowerShell does not reliably populate PSScriptRoot while
# it is binding parameter defaults. This exact mistake broke Check-LiveBuild.ps1 first, and I made
# it again here — hence the comment, so the next script does not make it a third time.
if (-not $Path) {
    $here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $Path = Join-Path (Split-Path -Parent $here) 'last-summary.md'
}

if (-not (Test-Path $Path)) {
    Write-Host "NO SUMMARY: $Path does not exist." -ForegroundColor Red
    exit 1
}

$text = Get-Content -Raw -Path $Path

try {
    Set-Clipboard -Value $text
} catch {
    Write-Host "CLIPBOARD FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "The text is still readable at: $Path"
    exit 1
}

Start-Sleep -Milliseconds 300
$back = Get-Clipboard -Raw

# Compare without carriage returns — the clipboard normalises line endings, and that is not a
# failure. [char]13 avoids escape sequences, which is what kept breaking this.
$cr = [string][char]13
$a = $text.Replace($cr, '').TrimEnd()
$b = if ($null -eq $back) { '' } else { $back.Replace($cr, '').TrimEnd() }

if ($a -eq $b) {
    $lines = ($a -split ([string][char]10)).Count
    Write-Host "CLIPBOARD OK - $($a.Length) characters, $lines lines, verified by reading back." -ForegroundColor Green
    Write-Host "Also saved at: $Path"
    exit 0
} else {
    Write-Host "CLIPBOARD MISMATCH - wrote $($a.Length) characters, read back $($b.Length)." -ForegroundColor Red
    Write-Host "Do not trust the clipboard. Open the file instead: $Path"
    exit 1
}
