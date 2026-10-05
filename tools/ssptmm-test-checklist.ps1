# SSPTMM - manual test checklist. Started by SSPTMM-test-checklist.bat in the repository root.
#
# Walks through testing a release on a FRESH SPT install, one step at a time, and writes what you
# found to test-reports\. For each step: do it, then answer P (passed), F (failed) or S (skipped)
# and a note in your own words - what you did and what you saw. Every answer is saved as soon as
# you give it, so stopping part-way (Ctrl+C) keeps everything so far.
#
# Why: sp-mod.com's content guidelines - "Mod authors must thoroughly test their submissions using
# a fresh SPT installation" and "All advertised features must work as described".
#
# Written for Windows PowerShell 5.1, which every Windows 10 and 11 PC has.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$steps = @(
    @{ Section = 'Setup' }
    'Make a FRESH SPT install: a new folder, a clean copy of SPT, no mods. Start the server and the game once, so SPT makes its folders and a profile. Close both.'
    'Unzip the SSPTMM release zip into that SPT folder. You now have SPT\SSPTMM\SSPTMM.exe, and nothing else new beside the game.'
    'Run SSPTMM\SSPTMM.exe. The first time, it asks whether it may check GitHub for new releases. Answer it.'
    'Options (gear, top right): set SPT Install Folder to the fresh install and press Save. The window title shows its SPT version.'

    @{ Section = 'Workshop' }
    'Workshop > Browse: filter by your SPT version, search for a mod, change the sort. The results make sense.'
    'Hover a card (its pictures slide by), open Quick View (magnifier), open an item page. The Description, Comments, Change Notes and Versions tabs all show.'
    'Search the Workshop for SSPTMM and for TCF Mod Manager. Neither appears as a mod you can subscribe to.'

    @{ Section = 'Subscribe' }
    'Subscribe to a mod that needs others (e.g. SAIN, which needs BigBrain and Waypoints). The required-items question appears; Subscribe to All installs all of them.'
    'Start the server and the game. The game reaches the main menu and the mods load (BepInEx\LogOutput.log shows no errors from them).'
    'Subscribe to SVM (Server Value Modifier). The Play page''s Mod tools card lists Greed, and Open starts it.'
    'Download only on any mod: the file is saved, nothing in SPT changes.'
    'Install from file: install a mod from an archive you downloaded yourself. It shows up in Subscribed items.'

    @{ Section = 'Subscribed items' }
    'Switch a mod off and on with its switch. Its folder moves into .disabled and back, and the game follows.'
    'Presets: save the current setup as a preset, use Disable all mods, then apply your preset. Every mod is back as it was. Undo and Put back work.'
    'Unsubscribe from a mod. Its files are gone from SPT. Undo puts them back.'
    'Update: if an installed mod has an update, update it (S if none has). The new version is installed.'

    @{ Section = 'Collections' }
    'Collections > Browse: open a public collection and use Subscribe to all on a small one. Its items install at versions for your SPT.'
    'Your collections > Create from installed mods. Open it, Share with friends > Copy share code. Add from code with that code gives the same list.'

    @{ Section = 'Play' }
    'Play: Start server, read the server log on the page, Stop server.'
    'Start the game the usual way, then Close game from SSPTMM. With "Close the game when the server stops" ticked, Stop server closes the game too.'
    'Experimental: Options > Start the game from SSPTMM on, pick a profile, press PLAY. Server and game start with no SPT launcher, and you can play a raid. Switch it off again afterwards.'

    @{ Section = 'Diagnose logs' }
    'Tools > Diagnose logs after a game session: what it reports matches what happened. Copy a report for help: your Windows user name is not in the copied text.'

    @{ Section = 'Safety and removal' }
    'Options > SPT profile backups lists copies made before your installs. Put back restores one (with SPT closed).'
    'Unsubscribe from every mod. The SPT install starts and plays as it did before SSPTMM.'
    'Uninstall: switch update notifications off if you turned them on, close SSPTMM, delete SPT\SSPTMM and the SSPTMM folders in %TEMP%. Nothing of SSPTMM is left.'
)

function Ask([string] $prompt) {
    Write-Host -NoNewline $prompt
    $answer = [Console]::ReadLine()
    if ($null -eq $answer) { throw 'No more input.' }
    return $answer.Trim()
}

# A table cell: one line, and a "|" would end the cell early.
function Cell([string] $text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return '-' }
    return ($text -replace '[\r\n]+', ' ') -replace '\|', '/'
}

# The version, from the source - the same place the release script reads it.
$version = 'unknown'
$props = Join-Path $root 'build\Directory.Build.props'
if (Test-Path $props) {
    $m = [regex]::Match([IO.File]::ReadAllText($props), '<Version>([^<]+)</Version>')
    if ($m.Success) { $version = $m.Groups[1].Value }
}

$reports = Join-Path $root 'test-reports'
New-Item -ItemType Directory -Force -Path $reports | Out-Null
$stamp = Get-Date -Format 'yyyy-MM-dd_HHmm'
$report = Join-Path $reports "SSPTMM-$version-test-$stamp.md"
$utf8 = New-Object Text.UTF8Encoding $false
function Write-Report([string] $line) { [IO.File]::AppendAllText($report, $line + "`r`n", $utf8) }

Write-Host ''
Write-Host "SSPTMM $version - test checklist"
Write-Host "The report goes to test-reports\$(Split-Path -Leaf $report)"
Write-Host ''
$tester = Ask 'Your name, as it should appear in the report: '
$sptVersion = Ask 'SPT version of the fresh install you are testing on (e.g. 4.1.6): '
$sptFolder = Ask 'Its folder (e.g. D:\SPT-test): '
$windows = $null
try { $windows = (Get-CimInstance Win32_OperatingSystem).Caption } catch { }
if (-not $windows) { $windows = [Environment]::OSVersion.VersionString }

[IO.File]::WriteAllText($report, '', $utf8)
Write-Report "# SSPTMM $version - test report"
Write-Report ''
Write-Report "- Tester: $(Cell $tester)"
Write-Report "- Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Report "- Windows: $windows"
Write-Report "- SPT: $(Cell $sptVersion), a fresh install at $(Cell $sptFolder)"
Write-Report "- SSPTMM: $version"
Write-Report ''
Write-Report 'Result: Pass, FAIL or Skipped.'
Write-Report ''
Write-Report '| # | Step | Result | Notes |'
Write-Report '|---|---|---|---|'

$n = 0
$counts = @{ Pass = 0; FAIL = 0; Skipped = 0 }
foreach ($step in $steps) {
    if ($step -is [hashtable]) {
        Write-Host ''
        Write-Host "==== $($step.Section) ====" -ForegroundColor Cyan
        Write-Report "| | **$($step.Section)** | | |"
        continue
    }

    $n++
    Write-Host ''
    Write-Host "[$n] $step"
    do {
        $r = (Ask '    Result - P, F or S: ').ToUpperInvariant()
    } until ($r -in 'P', 'F', 'S')
    $result = @{ P = 'Pass'; F = 'FAIL'; S = 'Skipped' }[$r]
    $counts[$result]++
    $note = Ask '    Note (what you did and saw): '
    $shown = if ($result -eq 'FAIL') { '**FAIL**' } else { $result }
    Write-Report "| $n | $(Cell $step) | $shown | $(Cell $note) |"
}

Write-Report ''
Write-Report "Totals: $($counts.Pass) passed, $($counts.FAIL) failed, $($counts.Skipped) skipped."
$overall = Ask "`nAnything else to note overall (one line, or press Enter): "
if ($overall) {
    Write-Report ''
    Write-Report '## Overall'
    Write-Report ''
    Write-Report ($overall -replace '[\r\n]+', ' ')
}

Write-Host ''
Write-Host "Done: $($counts.Pass) passed, $($counts.FAIL) failed, $($counts.Skipped) skipped."
Write-Host "The report: test-reports\$(Split-Path -Leaf $report)"
Write-Host 'Read it through, fix anything that failed, and test again.'
