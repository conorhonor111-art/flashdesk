<#
    Fix-UiUxProMaxPython.ps1 -- re-applies the python3 -> python fix to the globally installed
    ui-ux-pro-max skill.

    WHY THIS EXISTS. `uipro update --global` and `uipro init --global` regenerate SKILL.md from a
    template every time they run, and that template says `python3` in every example -- which on
    this machine resolves to the Windows Store's execution-alias stub and fails with "Python was
    not found", even though a real Python 3 interpreter is installed and working
    (C:\Python314\python.exe, reachable as `python` and as `py`). A hand edit to that file does not
    survive the next update or reinstall; it is regenerated and the fix is silently gone. This
    script is the survivable version of the same one-line fix -- safe to re-run any time, including
    right after every `uipro update` / `uipro init`.

    Plain ASCII only in this file, deliberately: an em dash in a UTF-8-without-BOM script is exactly
    the kind of thing Windows PowerShell 5.1 misreads as ANSI and turns into a parse error -- this
    project has hit that trap more than once in other files, and the fix here is simply not to carry
    a character that trap depends on.

    WHAT THIS DOES NOT TOUCH: this repository (the file it edits is entirely outside it, in the
    user's global Claude skills folder), the system PATH, and nothing is installed. Deliberately not
    a PATH-level `python3` shim: that would be a machine-wide change for a problem that is really
    just one file's wording.

    Double-click Fix-UiUxProMaxPython.cmd, or: powershell -File scripts\Fix-UiUxProMaxPython.ps1
#>

[CmdletBinding()]
param(
    [string] $SkillMd = (Join-Path $env:USERPROFILE ".claude\skills\ui-ux-pro-max\SKILL.md")
)

$ErrorActionPreference = 'Stop'

Write-Host ''
Write-Host '================================================================'
Write-Host ' ui-ux-pro-max -- re-apply the python3 -> python fix'
Write-Host '================================================================'
Write-Host ''

if (-not (Test-Path $SkillMd)) {
    Write-Host "Not installed globally -- nothing to fix. Expected to find:" -ForegroundColor Yellow
    Write-Host "  $SkillMd"
    exit 0
}

# Marker unique to OUR note (see below) -- the one reliable way to tell "already fixed" apart from
# "never fixed", since the word python3 also appears, correctly, inside our own explanation of why
# it was changed. Checking for bare "python3" would never reach zero once that note exists.
$marker = 'Fix-UiUxProMaxPython.ps1'

$before = Get-Content -Raw -Encoding UTF8 -Path $SkillMd
if ($before -like "*$marker*") {
    Write-Host "Already fixed -- this file already carries our note. Nothing to do." -ForegroundColor Green
    Write-Host "(If uipro just regenerated this file and you are seeing this, the regenerated" -ForegroundColor DarkGray
    Write-Host " version happened to match ours already -- unusual, but not a problem.)" -ForegroundColor DarkGray
    exit 0
}

$python3Count = ([regex]::Matches($before, 'python3\s')).Count
if ($python3Count -eq 0) {
    Write-Host "No 'python3 ' commands found -- nothing to fix." -ForegroundColor Green
    exit 0
}

$after = $before -replace 'python3 ', 'python '

# The version-check line becomes a redundant `python --version || python --version` after the plain
# substitution above -- collapsed back to one honest check.
$after = $after -replace 'python --version \|\| python --version', 'python --version'

# The upstream note, if present, now contradicts itself (both sides say "python"). Replaced with one
# that explains what happened and why, and points at this script for next time.
$oldNote = '> **Note:** On Windows, use `python` instead of `python3` to run scripts (e.g., `python scripts/search.py` instead of `python scripts/search.py`).'
$newNote = @'
> **Note (kept current by scripts\Fix-UiUxProMaxPython.ps1 in the FlashDesk repo):** every command in
> this file originally read `python3`, which on this machine resolves to the Windows Store's
> execution-alias stub and fails with "Python was not found" even though a real interpreter is
> installed (`C:\Python314\python.exe`, reachable as `python` and `py`). `uipro update`/`uipro init`
> regenerate this file from a template and silently undo this fix -- re-run that script after either.
'@
$after = $after -replace [regex]::Escape($oldNote), $newNote

Set-Content -Path $SkillMd -Value $after -Encoding UTF8 -NoNewline

$afterCount = ([regex]::Matches($after, 'python3\s')).Count
Write-Host "Fixed: $SkillMd" -ForegroundColor Green
Write-Host "  'python3 ' commands before : $python3Count"
Write-Host "  'python3 ' commands after  : $afterCount"
if ($afterCount -gt 0) {
    Write-Host "  Some remain -- the file's template may have changed shape. Open it and check by hand." -ForegroundColor Yellow
}
Write-Host ''
